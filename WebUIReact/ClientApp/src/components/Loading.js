import React from 'react'
import './Loading.css';

const Loading = ({message}) => {
    return (
        <div style={{margin: '5px 20px'}}>
        <h3 style={{color: 'white'}}>{message}</h3>
        <section class="wrapper">
            <div class="spinner">
                <i></i>
                <i></i>
                <i></i>
                <i></i>
                <i></i>
                <i></i>
                <i></i>
            </div>
        </section>
    </div>
    )
}

export default Loading
